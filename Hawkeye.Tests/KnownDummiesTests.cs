using Hawkeye.Engine.Services.Entity;

namespace Hawkeye.Tests;

/// <summary>
/// Owner, 2026-11-23: "it used to capture dummies before, but doesn't now". Hawkeye had restarted (an update) while he
/// stood at a dummy, so it never saw the dummy appear and took it for trash. Dummies keep their entity id (same ids
/// on 2026-11-21 and 2026-11-22 in his recordings), so a dummy seen once is known from then on.
/// </summary>
public sealed class KnownDummiesTests : IDisposable
{
    // The owner's Punching Bag from both recordings: entity 27610 (DA D7 01), mob 2400032.
    private const int DummyId = 27610, DummyCode = 2400032;
    private readonly string path = Path.Combine(Path.GetTempPath(), $"hawkeye-dummies-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(path);

    [Fact]
    public void ADummySeenOnce_IsStillADummy_AfterARestart()
    {
        var before = new EntityTracker { Dummies = new KnownDummies(path) };
        before.CreateOrUpdateTargetEntity(DummyId, DummyCode, 119_700);      // it appeared: Hawkeye saw it
        Assert.True(before.GetTargetMob(DummyId)!.IsBoss);

        var after = new EntityTracker { Dummies = new KnownDummies(path) };   // restarted next to it: no appear message
        var mob = after.GetOrCreateTargetEntity(DummyId);
        Assert.Equal(DummyCode, mob.MobCode);
        Assert.True(mob.IsDummy);
        Assert.True(mob.IsBoss);   // so the boss-only rule lets the fight open
    }

    [Fact]
    public void WithoutIt_TheDummyLooksLikeTrash()
    {
        var after = new EntityTracker();
        Assert.False(after.GetOrCreateTargetEntity(DummyId).IsBoss);
    }

    [Fact]
    public void AfterARestart_YouAreNamedFromYourLastCharacterOfThatClass()
    {
        var odylePath = Path.Combine(Path.GetTempPath(), $"hawkeye-chars-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(odylePath, """
                [ { "Name": "Alpha", "ClassId": 14, "Current": 1, "Pool": 1, "VerifiedAt": "2026-11-23T12:26:34" },
                  { "Name": "Charlie", "ClassId": 14, "Current": 1, "Pool": 1, "VerifiedAt": "2026-11-20T10:00:00" },
                  { "Name": "Bravo", "ClassId": 17, "Current": 1, "Pool": 1, "VerifiedAt": "2026-11-23T12:30:00" } ]
                """);
            var entities = new EntityTracker();
            _ = new Hawkeye.Engine.Services.Odyle.OdyleTracker(entities, odylePath);
            string? named = null;
            entities.CurrentUserNamed += n => named = n;

            entities.MarkCurrentPlayer(9377);                     // worked out from casts; class not known yet
            Assert.Null(named);
            var ranger = new Hawkeye.Core.Models.CharacterClass { Id = 14, Name = "Ranger" };
            entities.GetOrCreateSessionPlayer(9377, ranger);      // the class arrives with your first hit
            Assert.Equal("Alpha", named);                         // the ranger you played last, not Charlie, not Bravo
            Assert.Equal("Alpha", entities.GetPlayerEntity(9377)!.Name);
        }
        finally { File.Delete(odylePath); }
    }

    [Fact]
    public void OnlyDummiesAreKept_AndPlayersAreNeverTakenForOne()
    {
        var tracker = new EntityTracker { Dummies = new KnownDummies(path) };
        tracker.CreateOrUpdateTargetEntity(500, 2101115, 1_350_000);          // a boss: respawns with a new id, not kept
        tracker.CreateOrUpdateTargetEntity(DummyId, DummyCode);
        Assert.Equal(1, new KnownDummies(path).Count);

        var later = new EntityTracker { Dummies = new KnownDummies(path) };
        later.SetSessionPlayerName(DummyId, "Bravo", 0);                     // that id is a player now
        Assert.Equal(0, later.GetOrCreateTargetEntity(DummyId).MobCode);
    }
}
