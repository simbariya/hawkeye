using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// Hawkeye started mid-session never sees the login message that says which entity is you.
/// These pin down how it works that out from your own casts, what the tracker does meanwhile,
/// and (I10) that a guess never overrides what the server said.
/// </summary>
public sealed class TrackerIdentityTests : IDisposable
{
    private const int Me = 77, Other = 88;
    private const int MarkingShot = 14090000, Deadshot = 14010000, ArrowStorm = 14050000;

    private readonly string path = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
    private readonly EntityTracker entities = new();
    private readonly TimedEventTracker cooldowns = new();
    private readonly TimedEventTracker buffs = new();
    private readonly CombatSessionManager sessions;

    public TrackerIdentityTests()
    {
        var settings = new SettingsService(path);
        settings.Update(s =>
        {
            s.TrackedSkills.Add(new TrackedSkill { SkillId = MarkingShot, Mode = TrackMode.Buff });
            s.TrackedSkills.Add(new TrackedSkill { SkillId = Deadshot, Mode = TrackMode.Cooldown });
        });
        sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, settings, buffs, cooldowns);
    }

    public void Dispose() => File.Delete(path);

    private void Cast(int entity, int skill, int cdMs) => sessions.RegisterSkillCastEvent(new SkillCastEvent
    {
        EntityId = entity, SkillId = skill, Name = "x", IconUrl = null, Cd = TimeSpan.FromMilliseconds(cdMs),
    });

    private void CooldownUpdate(int skill, int ms) => sessions.RegisterSkillCdUpdateEvent(new TimedEvent
    {
        Id = (uint)skill, Name = "x", IconUrl = "", Duration = TimeSpan.FromMilliseconds(ms),
    });

    private void Buff(int onEntity, int caster, int skill, uint ms) => sessions.ProcessBuffEvent(new BuffEvent
    {
        EntityId = onEntity, CasterId = caster, BuffId = skill, BuffName = "x", DurationMs = ms, AppliedAt = DateTime.Now,
    });

    /// <summary>Two of your casts, each starting its cooldown: enough evidence to name you.</summary>
    private void TwoEchoes(int entity)
    {
        Cast(entity, Deadshot, 12000);
        CooldownUpdate(Deadshot, 12000);
        Cast(entity, ArrowStorm, 20000);
        CooldownUpdate(ArrowStorm, 20000);
    }

    [Fact]
    public void TwoCastsThatStartTheirCooldowns_IdentifyYou()
    {
        Assert.False(entities.HasCurrentUser);
        Cast(Other, MarkingShot, 20000);   // someone else casts something without a cooldown echo
        TwoEchoes(Me);

        Assert.True(entities.IsCurrentPlayer(Me));
        Assert.False(entities.IsCurrentPlayer(Other));
    }

    [Fact]
    public void OneEcho_IsNotEnoughEvidence()
    {
        Cast(Me, Deadshot, 12000);
        CooldownUpdate(Deadshot, 12000);
        Assert.False(entities.HasCurrentUser);
    }

    [Fact]
    public void CooldownUpdateArrivingBeforeTheCast_StillCounts()
    {
        CooldownUpdate(Deadshot, 12000);
        Cast(Me, Deadshot, 12000);
        CooldownUpdate(ArrowStorm, 20000);
        Cast(Me, ArrowStorm, 20000);
        Assert.True(entities.IsCurrentPlayer(Me));
    }

    [Fact]
    public void RegularCooldownList_WithReadySkills_IsNotEvidence()
    {
        // I10: the server re-sends your whole cooldown list with "0 ms left" for ready skills.
        // Another ranger casting the same skills at that moment must not become you.
        CooldownUpdate(Deadshot, 0);
        CooldownUpdate(ArrowStorm, 0);
        Cast(Other, Deadshot, 12000);
        Cast(Other, ArrowStorm, 20000);
        CooldownUpdate(Deadshot, 0);
        CooldownUpdate(ArrowStorm, 0);
        Cast(Other, Deadshot, 12000);
        CooldownUpdate(Deadshot, 0);

        Assert.False(entities.HasCurrentUser);
    }

    [Fact]
    public void ServerNamedYou_AGuessNeverOverridesIt()
    {
        // I10: you were known from the login message, then the guess flipped you to another ranger.
        entities.SetSessionPlayerName(Me, "Owner", 0, isUser: true);
        TwoEchoes(Other);
        TwoEchoes(Other);

        Assert.True(entities.IsCurrentPlayer(Me));
        Assert.False(entities.IsCurrentPlayer(Other));
    }

    [Fact]
    public void ServerNamingYouLater_ReplacesAWrongGuess()
    {
        TwoEchoes(Other);
        Assert.True(entities.IsCurrentPlayer(Other));

        entities.SetSessionPlayerName(Me, "Owner", 0, isUser: true);
        Assert.True(entities.IsCurrentPlayer(Me));
        Assert.False(entities.IsCurrentPlayer(Other));
    }

    [Fact]
    public void TrackedBuff_IsShownEvenBeforeYouAreKnown()
    {
        Buff(onEntity: 5000, caster: Me, MarkingShot, 15000);
        Assert.NotNull(buffs.Get(MarkingShot));
    }

    [Fact]
    public void OnceYouAreKnown_SomeoneElsesBuffIsIgnored_AndYoursCounts()
    {
        TwoEchoes(Me);

        Buff(onEntity: Other, caster: Other, MarkingShot, 15000);
        Assert.Null(buffs.Get(MarkingShot));

        Buff(onEntity: Me, caster: 161, MarkingShot, 10000);
        var t = buffs.Get(MarkingShot);
        Assert.NotNull(t);
        Assert.Equal(10000, t.Duration.TotalMilliseconds);
    }

    [Fact]
    public void Recast_RestartsTheBuff_EvenWhenTheGameSendsNoBuffMessage()
    {
        // I11: some recasts of Marking Shot came with no buff message at all.
        entities.SetSessionPlayerName(Me, "Owner", 0, isUser: true);
        Cast(Me, MarkingShot, 8350);
        Buff(onEntity: Me, caster: 161, MarkingShot, 15000);   // first cast: the game says 15 s
        buffs.Clear();                                          // as if it ran out

        Cast(Me, MarkingShot, 8350);                            // recast, no buff message follows
        var t = buffs.Get(MarkingShot);
        Assert.NotNull(t);
        Assert.Equal(15000, t.Duration.TotalMilliseconds);

        buffs.Clear();
        Cast(Other, MarkingShot, 8350);                         // someone else's cast does nothing
        Assert.Null(buffs.Get(MarkingShot));
    }

    [Fact]
    public void YourCastStartsTheCooldownTimer_OnceYouAreKnown()
    {
        TwoEchoes(Me);
        cooldowns.Clear();
        Cast(Me, Deadshot, 12000);
        Assert.NotNull(cooldowns.Get(Deadshot));
    }
}
