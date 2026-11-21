using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;

namespace Hawkeye.Tests;

public sealed class TrackerAndSettingsTests
{
    private static TimedEvent Timer(uint id, TimeSpan duration) =>
        new() { Id = id, Name = "test", IconUrl = "", Duration = duration };

    [Fact]
    public void Timer_ReportsTimeLeft_ThenDisappearsWhenDone()
    {
        var timers = new TimedEventTracker();
        timers.Register(Timer(14090000, TimeSpan.FromMilliseconds(150)));

        var running = timers.Get(14090000);
        Assert.NotNull(running);
        Assert.InRange(running.TimeLeft.TotalMilliseconds, 1, 150);
        Assert.Null(timers.Get(14010000)); // never started

        Thread.Sleep(200);
        Assert.Null(timers.Get(14090000));
    }

    [Fact]
    public void Timer_ZeroDuration_ClearsARunningTimer()
    {
        // The game sends 0 ms left when a cooldown is reset; the icon should go back to "ready".
        var timers = new TimedEventTracker();
        timers.Register(Timer(1, TimeSpan.FromSeconds(30)));
        timers.Register(Timer(1, TimeSpan.Zero));
        Assert.Null(timers.Get(1));
    }

    [Fact]
    public void Settings_SurviveARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
        try
        {
            var first = new SettingsService(path);
            first.Update(s =>
            {
                s.Opacity = 0.5;
                s.ShowFps = false;
                s.TrackedSkills.Add(new TrackedSkill { SkillId = 14090000, Mode = TrackMode.Buff, Left = 100, Top = 200 });
            });

            var second = new SettingsService(path);
            Assert.Equal(0.5, second.Current.Opacity);
            Assert.False(second.Current.ShowFps);
            var tracked = Assert.Single(second.Current.TrackedSkills);
            Assert.Equal(TrackMode.Buff, tracked.Mode);
            Assert.Equal(100, tracked.Left);
            Assert.Equal(200, tracked.Top);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var settings = new SettingsService(path);
            Assert.Equal(0.92, settings.Current.Opacity);
            Assert.Empty(settings.Current.TrackedSkills);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
