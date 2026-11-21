using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;

namespace Hawkeye.Tests;

public sealed class DamageStatisticsTests
{
    [Fact]
    public void DamageStatisticsCalculator_AggregatesDamageAndExcludesDotFromHitCounters()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var session = new PlayerSession(42, new EntityTracker());
        session.AddDamage(new PlayerDamage
        {
            DateTime = start,
            Damage = 40,
            IsCritical = true,
            IsBackAttack = true,
        });
        session.AddDamage(new PlayerDamage
        {
            DateTime = start.AddSeconds(1),
            Damage = 30,
            IsDot = true,
            IsCritical = true,
        });
        session.AddDamage(new PlayerDamage
        {
            DateTime = start.AddSeconds(2),
            Damage = 30,
        });

        var stats = DamageStatisticsCalculator.ComputePlayerStats(session, totalCombatDamage: 200);

        Assert.Equal(100, session.TotalDamage);
        Assert.Equal(2, session.HitCount);
        Assert.Equal(100, stats.TotalDamage);
        Assert.Equal(2, stats.HitCount);
        Assert.Equal(1, stats.CriticalHits);
        Assert.Equal(1, stats.BackAttacks);
        Assert.Equal(50, stats.DamagePerSecond);
        Assert.Equal(50, stats.DamagePercentage);
        Assert.Equal("Unknown player 42", stats.PlayerName);
    }
}
