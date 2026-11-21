using Hawkeye.Engine.Services.Timed;

namespace Hawkeye.Tests;

/// <summary>
/// How a running cooldown follows the game's "time left" updates (owner 2026-11-21: Deadshot's sweep
/// went wrong whenever another skill cut its cooldown by a second).
/// </summary>
public sealed class TimerTests
{
    private const uint Deadshot = 14010000;
    private readonly TimedEventTracker timers = new();

    private static TimedEvent Left(double seconds) => new()
    {
        Id = Deadshot, Name = "Deadshot", IconUrl = "", Duration = TimeSpan.FromSeconds(seconds),
    };

    [Fact]
    public void FirstUpdateRightAfterTheCast_SetsTheRealFullLength()
    {
        timers.Register(Left(16.75));          // the cast knows only the base cooldown
        timers.UpdateTimeLeft(Left(15.4));     // the game: 15.4 s left, moments later

        var t = timers.Get(Deadshot)!;
        Assert.InRange(t.Duration.TotalSeconds, 15.4, 15.6);   // full length = real cooldown
        Assert.InRange(t.TimeLeft.TotalSeconds, 15.3, 15.4);
    }

    [Fact]
    public void CooldownCutLater_KeepsTheFullLength_AndOnlyMovesTimeLeft()
    {
        timers.UpdateTimeLeft(Left(15));
        Thread.Sleep(1600);                    // past the settle window
        timers.UpdateTimeLeft(Left(12.4));     // another skill took a second off

        var t = timers.Get(Deadshot)!;
        Assert.InRange(t.Duration.TotalSeconds, 14.99, 15.01); // the sweep keeps its scale
        Assert.InRange(t.TimeLeft.TotalSeconds, 12.3, 12.4);   // and jumps forward
    }

    [Fact]
    public void LongerThanTheRunningTimer_StartsANewOne_AndZeroClears()
    {
        timers.UpdateTimeLeft(Left(5));
        timers.UpdateTimeLeft(Left(9));        // longer than its full length: a new cooldown
        Assert.InRange(timers.Get(Deadshot)!.Duration.TotalSeconds, 8.99, 9.01);

        timers.UpdateTimeLeft(Left(0));        // reset: ready again
        Assert.Null(timers.Get(Deadshot));
    }
}
