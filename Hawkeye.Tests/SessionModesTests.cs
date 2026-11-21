using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// Which fights count, deaths per boss across pulls, the sticky meter, and the two dummy modes.
/// Uses real mob codes from mobs.json: 2090027 is a boss, 2000000 a training dummy, 1 is unknown (trash).
/// </summary>
public sealed class SessionModesTests : IDisposable
{
    private const int Me = 77, Ally = 88, Stranger = 99;
    private const int BossCode = 2090027, DummyCode = 2000000, TrashCode = 1;

    private readonly string path = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
    private readonly EntityTracker entities = new();
    private readonly SettingsService settings;
    private readonly CombatSessionManager sessions;
    private readonly DateTime t0 = DateTime.Now.AddMinutes(-10);

    public SessionModesTests()
    {
        settings = new SettingsService(path);
        sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, settings, new TimedEventTracker(), new TimedEventTracker());
        entities.SetSessionPlayerName(Me, "Owner", 0, isUser: true);
        entities.SetSessionPlayerName(Ally, "Ally", 0);
        entities.SetSessionPlayerName(Stranger, "Stranger", 0);
    }

    public void Dispose() => File.Delete(path);

    private Mob Target(int entityId, int code)
    {
        var mob = entities.CreateOrUpdateTargetEntity(entityId, code, 1_000_000);
        if (mob.HpCurrent == 0) mob.HpCurrent = 1_000_000;
        return mob;
    }

    private void Hit(int player, Mob target, double atSeconds, long damage = 100) =>
        sessions.ProcessDamageEvent(new PlayerDamage
        {
            DateTime = t0.AddSeconds(atSeconds),
            SourceEntity = new Player { Id = player },
            TargetEntity = target,
            Damage = damage,
        });

    private long Total(int player) => sessions.PlayerStats.FirstOrDefault(p => p.PlayerId == player)?.TotalDamage ?? 0;

    [Fact]
    public void OnlyBossesAndDummiesOpenAFight()
    {
        Hit(Me, Target(1, TrashCode), 0);
        Assert.Empty(sessions.PlayerStats);

        Hit(Me, Target(2, BossCode), 1);
        Assert.Equal(100, Total(Me));

        Hit(Me, Target(3, DummyCode), 2);
        Assert.Equal(100, Total(Me));
    }

    [Fact]
    public void MeterSticksToYourFight_NotTheBusiestOneNearby()
    {
        var mine = Target(1, BossCode);
        var theirs = Target(2, DummyCode);
        Hit(Me, mine, 0);
        for (int i = 1; i <= 20; i++) Hit(Stranger, theirs, i * 0.5); // 10 s of someone else's fight

        Assert.Equal(1, sessions.GetActiveTargetInfo()?.Id);
    }

    [Fact]
    public void BossFight_IsOnWhileHitsLand_AndOff15SecondsAfter()
    {
        Assert.False(sessions.IsInBossFight(t0));               // nothing yet
        Hit(Me, Target(1, TrashCode), 0);
        Assert.False(sessions.IsInBossFight(t0.AddSeconds(1))); // trash is not a boss fight

        Hit(Me, Target(2, BossCode), 10);
        Assert.True(sessions.IsInBossFight(t0.AddSeconds(12)));
        Assert.True(sessions.IsInBossFight(t0.AddSeconds(25)));
        Assert.False(sessions.IsInBossFight(t0.AddSeconds(26)));
    }

    [Fact]
    public void StoppedDummyRun_IsNotABossFight()
    {
        settings.Update(s => s.DummyMode = DummyMode.StartStop);
        var dummy = Target(1, DummyCode);
        sessions.StartDummyRun();
        Hit(Me, dummy, 0);
        Assert.True(sessions.IsInBossFight(t0.AddSeconds(1)));
        sessions.StopDummyRun();
        Assert.False(sessions.IsInBossFight(t0.AddSeconds(1)));
    }

    [Fact]
    public void DeathsAddUpAcrossPulls_AndStartAgainAfterTheKill()
    {
        var boss = Target(1, BossCode);
        Hit(Me, boss, 0);
        Hit(Ally, boss, 1);

        sessions.RegisterPlayerDeath(Ally);
        sessions.RegisterPlayerDeath(Ally);
        sessions.RegisterPlayerDeath(Stranger);   // never hit this boss: not counted
        Assert.Equal(2, sessions.GetActiveBossDeaths()[Ally]);
        Assert.False(sessions.GetActiveBossDeaths().ContainsKey(Stranger));

        boss.HpCurrent = 0;                        // the kill
        Hit(Me, boss, 2);
        sessions.RegisterPlayerDeath(Ally);        // after the kill: not counted
        Assert.Equal(2, sessions.GetActiveBossDeaths()[Ally]);

        boss.HpCurrent = 1_000_000;                // next time you fight it
        Hit(Me, boss, 600);
        Assert.Empty(sessions.GetActiveBossDeaths());
    }

    [Fact]
    public void TimedDummy_StopsAtTheChosenLength_AndKeepsTheResultWhileYouKeepHitting()
    {
        settings.Update(s => { s.DummyMode = DummyMode.Timed; s.DummyMinutes = 2; });
        var dummy = Target(1, DummyCode);

        for (double t = 0; t < 120; t += 5) Hit(Me, dummy, t);     // 24 hits inside the 2 minutes
        Hit(Me, dummy, 119);
        for (double t = 125; t < 160; t += 5) Hit(Me, dummy, t);   // past 2 min, no 10 s pause

        Assert.Equal(2500, Total(Me));
        Assert.Equal(TimeSpan.FromSeconds(119), sessions.GetCombatDuration());
    }

    [Fact]
    public void TimedDummy_ANewRunStartsAfterA10SecondPause()
    {
        settings.Update(s => { s.DummyMode = DummyMode.Timed; s.DummyMinutes = 1; });
        var dummy = Target(1, DummyCode);

        Hit(Me, dummy, 0);
        Hit(Me, dummy, 59);
        Hit(Me, dummy, 65);        // after the minute: dropped, but it is still you hitting
        Hit(Me, dummy, 80, 7);     // 15 s pause: a fresh run

        Assert.Equal(7, Total(Me));
    }

    [Fact]
    public void StartStopDummy_CountsOnlyBetweenStartAndStop()
    {
        settings.Update(s => s.DummyMode = DummyMode.StartStop);
        var dummy = Target(1, DummyCode);

        Hit(Me, dummy, 0);                         // before Start: ignored
        Assert.Empty(sessions.PlayerStats);

        sessions.StartDummyRun();
        Hit(Stranger, dummy, 1);                   // someone else cannot start your run
        Assert.Empty(sessions.PlayerStats);

        Hit(Me, dummy, 2);                         // your first hit starts it
        Hit(Ally, dummy, 30);                      // others on the same dummy count
        Hit(Me, dummy, 62);                        // a long pause does not end it
        sessions.StopDummyRun();
        Hit(Me, dummy, 70);                        // after Stop: ignored

        Assert.Equal(200, Total(Me));
        Assert.Equal(100, Total(Ally));
        Assert.Equal(TimeSpan.FromSeconds(60), sessions.GetCombatDuration());

        sessions.StartDummyRun();                  // Start again clears the old numbers
        Assert.Empty(sessions.PlayerStats);
    }
}
