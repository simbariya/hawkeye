using Hawkeye.Engine.Services.Odyle;

namespace Hawkeye.Tests;

/// <summary>The global Odyle refill: +15 to current every 3 hours (16:00 UTC anchor), capped at 840.</summary>
public sealed class OdyleRefillTests
{
    private static DateTime Utc(int h, int m) => new(2026, 11, 21, h, m, 0, DateTimeKind.Utc);

    [Fact]
    public void AltB_250At1513_Is265After16()
    {
        // Real: read 250 at 15:13, the owner saw 265 after the 16:00 refill.
        var (current, estimated) = OdyleRefill.Estimate(250, Utc(15, 13), Utc(16, 30));
        Assert.Equal(265, current);
        Assert.True(estimated);
    }

    [Fact]
    public void NoRefillSinceTheReading_IsExact()
    {
        var (current, estimated) = OdyleRefill.Estimate(250, Utc(16, 5), Utc(18, 59));
        Assert.Equal(250, current);
        Assert.False(estimated);
    }

    [Fact]
    public void RefillsEveryThreeHours_AndStopAtTheCap()
    {
        Assert.Equal(new DateTime(2026, 11, 21, 19, 0, 0, DateTimeKind.Utc), OdyleRefill.NextAfter(Utc(16, 0)));
        Assert.Equal(3, OdyleRefill.CountBetween(Utc(15, 0), Utc(22, 0)));          // 16:00, 19:00, 22:00
        Assert.Equal((840, true), OdyleRefill.Estimate(830, Utc(15, 0), Utc(22, 0)));
        Assert.Equal((840, false), OdyleRefill.Estimate(840, Utc(15, 0), Utc(22, 0)));
    }
}
