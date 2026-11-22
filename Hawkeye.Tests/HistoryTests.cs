using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// Fight history (owner 2026-11-21): the last 100 of YOUR fights (50 until v0.2.16), a kept-forever folder, and someone else's
/// fight never replacing yours on the meter.
/// </summary>
public sealed class HistoryTests : IDisposable
{
    private const int Me = 77, Stranger = 99, DummyCode = 2000000, BossCode = 2090027;
    private static readonly Skill Deadshot = new() { Id = 14010000, Name = "Deadshot" };
    private static readonly CharacterClass Ranger = new() { Id = 14, Name = "Ranger" };

    private readonly string dir = Path.Combine(Path.GetTempPath(), $"hawkeye-history-{Guid.NewGuid():N}");
    private readonly string settingsPath = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
    private readonly EntityTracker entities = new();
    private readonly SettingsService settings;
    private readonly EncounterHistory history;
    private readonly CombatSessionManager sessions;
    private readonly DateTime t0 = DateTime.Now.AddMinutes(-10);

    public HistoryTests()
    {
        settings = new SettingsService(settingsPath);
        history = new EncounterHistory(dir);
        sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, settings,
            new TimedEventTracker(), new TimedEventTracker(), history);
        entities.SetSessionPlayerName(Me, "Owner", 0, isUser: true);
        entities.SetSessionPlayerName(Stranger, "Stranger", 0);
    }

    public void Dispose()
    {
        File.Delete(settingsPath);
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private Mob Target(int id, int code)
    {
        var mob = entities.CreateOrUpdateTargetEntity(id, code, 1_000_000);
        if (mob.HpCurrent == 0) mob.HpCurrent = 1_000_000;
        return mob;
    }

    private void Hit(int player, Mob target, double atSeconds, long damage = 100, bool crit = false) =>
        sessions.ProcessDamageEvent(new PlayerDamage
        {
            DateTime = t0.AddSeconds(atSeconds),
            SourceEntity = new Player { Id = player },
            TargetEntity = target,
            Skill = Deadshot,
            CharacterClass = Ranger,
            Damage = damage,
            IsCritical = crit,
        });

    [Fact]
    public void YourFinishedFight_GoesToHistory_WithTheSkillBreakdown()
    {
        var dummy = Target(1, DummyCode);
        Hit(Me, dummy, 0, 100, crit: true);
        Hit(Me, dummy, 1, 300);
        sessions.CompleteActiveSessions();

        var e = Assert.Single(history.Recent);
        var you = Assert.Single(e.Players);
        Assert.True(you.IsUser);
        Assert.Equal(400, you.TotalDamage);
        var skill = Assert.Single(you.Skills);
        Assert.Equal("Deadshot", skill.Name);
        Assert.Equal(2, skill.Hits);
        Assert.Equal(1, skill.Critical);
        Assert.Equal(300, skill.MaxHit);
    }

    [Fact]
    public void SomeoneElsesFight_IsNeitherShownNorSaved()
    {
        var mine = Target(1, DummyCode);
        Hit(Me, mine, 0);
        sessions.CompleteActiveSessions();           // your run ends and is saved

        var theirs = Target(2, DummyCode);
        Hit(Stranger, theirs, 100);                  // a stranger starts on another dummy
        Assert.Empty(sessions.PlayerStats);          // the meter does not switch to it (it shows your last fight)

        sessions.CompleteActiveSessions();
        Assert.Single(history.Recent);               // only yours was saved
        Assert.Equal(Me, history.LastOwn!.You!.PlayerId);
    }

    [Fact]
    public void StrangerReusingYourDummyAfterYourRun_DoesNotReplaceIt()
    {
        settings.Update(s => { s.DummyMode = DummyMode.Timed; s.DummyMinutes = 1; });
        var dummy = Target(1, DummyCode);
        Hit(Me, dummy, 0, 500);
        Hit(Me, dummy, 5, 500);
        Hit(Stranger, dummy, 60);                    // 55 s later: your run closes, theirs starts

        Assert.Equal(1000, history.LastOwn!.You!.TotalDamage);
        Assert.Empty(sessions.PlayerStats);
    }

    [Fact]
    public void StoppedDummyRun_IsSavedOnce()
    {
        settings.Update(s => s.DummyMode = DummyMode.StartStop);
        var dummy = Target(1, DummyCode);
        sessions.StartDummyRun();
        Hit(Me, dummy, 0);
        sessions.StopDummyRun();
        Assert.Single(history.Recent);

        sessions.CompleteActiveSessions();           // closing Hawkeye later does not save it twice
        Assert.Single(history.Recent);
    }

    [Fact]
    public void KeepsTheLast100_KeptForeverSurvives_AndItAllReloadsFromDisk()
    {
        for (int i = 0; i < EncounterHistory.MaxRecent + 5; i++)
            history.Add(new EncounterSnapshot { TargetName = $"Fight {i}", StartedAt = t0.AddMinutes(i) });
        Assert.Equal(EncounterHistory.MaxRecent, history.Recent.Count);
        Assert.Equal($"Fight {EncounterHistory.MaxRecent + 4}", history.Recent[0].TargetName);

        var best = history.Recent[3];
        history.KeepForever(best.Id);
        history.FlushAsync().Wait();

        var reloaded = new EncounterHistory(dir);
        Assert.Equal(EncounterHistory.MaxRecent, reloaded.Recent.Count);
        var kept = Assert.Single(reloaded.Saved);
        Assert.Equal(best.TargetName, kept.TargetName);

        reloaded.DeleteSaved(kept.Id);
        Assert.Empty(new EncounterHistory(dir).Saved);
    }
}
