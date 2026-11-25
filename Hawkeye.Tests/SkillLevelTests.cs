using Hawkeye.Core.Models;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Skills;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// Skill levels from the game's skill list (0x5100) and the Compare window's rows (v0.2.34; owner, 2026-11-25:
/// compare fights "when the skills differ in level"). Records below are copied from real lists (skill ids and
/// levels only; nothing personal): a plain one, one with a running cooldown (flag 2) and one with flags 4 and 8.
/// </summary>
public sealed class SkillLevelTests : IDisposable
{
    // flags 01: Deadshot with specialisations 2 4 5, level 21 = learned 10 + 4 + 1 + 6.
    private static readonly byte[] Plain = Hex("01 22 D0 D5 00 15 0A 04 00 01 06 00 00 00 00 00");
    // flags 03: level 20 skill with 20,400 ms of cooldown left (varint B0 9F 01) before the last u32.
    private static readonly byte[] WithCooldown = Hex("03 70 5A DA 00 14 14 00 00 00 00 00 B0 9F 01 00 00 00 00");
    // flags 0D: level 12 = 10 + 2; flag 4 adds one byte (01), flag 8 a varint (D4 16).
    private static readonly byte[] WithExtras = Hex("0D 00 00 D7 00 0C 0A 02 00 00 00 00 00 00 00 00 01 D4 16");
    // A non-class entry (id 1003) as the lists start with.
    private static readonly byte[] General = Hex("01 EB 03 00 00 01 01 00 00 00 00 00 00 00 00 00");

    private readonly string dir = Path.Combine(Path.GetTempPath(), $"hawkeye-skills-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>A whole 0x5100 message: [length][00 51][count] then the records.</summary>
    private static byte[] Message(params byte[][] records)
    {
        var body = new List<byte> { 0x00, 0x51, (byte)records.Length };
        foreach (var r in records) body.AddRange(r);
        int length = body.Count + 2;
        return [(byte)(length & 0x7F | 0x80), (byte)(length >> 7), .. body];
    }

    [Fact]
    public void Parser_ReadsEveryRecordKind_ToTheLastByte()
    {
        var list = SkillListParser.Parse(Message(General, Plain, WithCooldown, WithExtras));

        Assert.NotNull(list);
        Assert.Equal(
            [(1003, 1, 1), (14012450, 21, 10), (14310000, 20, 20), (14090240, 12, 10)],
            list!.Select(e => (e.SkillId, e.Level, e.Learned)).ToArray());
    }

    [Fact]
    public void Parser_RefusesAListThatDoesNotEndExactly()
    {
        var full = Message(Plain, WithCooldown);
        Assert.Null(SkillListParser.Parse(full[..^1]));
        Assert.Null(SkillListParser.Parse([.. full, 0x00]));
    }

    [Fact]
    public void Levels_AreKeptPerSkillWithoutItsSpecialisations()
    {
        var levels = new SkillLevels(null, null);
        levels.OnSkillList(SkillListParser.Parse(Message(General, Plain, WithCooldown, WithExtras))!);

        Assert.Equal(21, levels.LevelOf(14010000));     // Deadshot, whatever specialisations a hit carries
        Assert.Equal(21, levels.LevelOf(14012450));
        Assert.Equal(20, levels.LevelOf(14310000));
        Assert.Equal(12, levels.LevelOf(14090000));
        Assert.Null(levels.LevelOf(1003));               // not a class skill
        Assert.Null(levels.LevelOf(15010000));           // another class: no list yet
    }

    [Fact]
    public void ANewList_ReplacesTheOldOne_AndOtherClassesKeepTheirs()
    {
        var levels = new SkillLevels(null, null);
        levels.OnSkillList([new(14010000, 21, 10)]);
        levels.OnSkillList([new(15010000, 9, 9)]);
        levels.OnSkillList([new(14010000, 16, 10)]);     // e.g. gear with less of a bonus, or another skill setup

        Assert.Equal(16, levels.LevelOf(14010000));
        Assert.Equal(9, levels.LevelOf(15010000));
    }

    [Fact]
    public void Levels_SurviveARestart()
    {
        var path = Path.Combine(dir, "skill-levels.json");
        new SkillLevels(path, null).OnSkillList([new(14010000, 21, 10), new(14310000, 20, 20)]);

        var again = new SkillLevels(path, null);
        Assert.Equal(21, again.LevelOf(14010000));
        Assert.Equal(20, again.LevelOf(14310000));
    }

    [Fact]
    public void Stamp_WritesYourLevels_OnlyIntoYourSkills()
    {
        var levels = new SkillLevels(null, null);
        levels.OnSkillList([new(14010000, 21, 10)]);
        var fight = new EncounterSnapshot
        {
            Players =
            [
                new EncounterPlayer { IsUser = true, Skills = [new EncounterSkill { SkillId = 14010000 }, new EncounterSkill { SkillId = 14990000 }] },
                new EncounterPlayer { IsUser = false, Skills = [new EncounterSkill { SkillId = 14010000 }] },
            ],
        };

        levels.Stamp(fight);

        Assert.Equal(21, fight.Players[0].Skills[0].Level);
        Assert.Equal(0, fight.Players[0].Skills[1].Level);   // not in the list: unknown, not guessed
        Assert.Equal(0, fight.Players[1].Skills[0].Level);   // someone else's: the game never sends theirs
    }

    [Fact]
    public void AFightSavedToHistory_CarriesYourSkillLevels()
    {
        var settingsPath = Path.Combine(dir, "settings.json");
        var entities = new EntityTracker();
        var history = new EncounterHistory(Path.Combine(dir, "history"));
        var levels = new SkillLevels(null, null);
        var sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, new SettingsService(settingsPath),
            new TimedEventTracker(), new TimedEventTracker(), history, levels);
        entities.SetSessionPlayerName(77, "Owner", 0, isUser: true);
        levels.OnSkillList([new(14010000, 20, 10)]);

        var dummy = entities.CreateOrUpdateTargetEntity(1, 2000000, 1_000_000);
        if (dummy.HpCurrent == 0) dummy.HpCurrent = 1_000_000;
        sessions.ProcessDamageEvent(new PlayerDamage
        {
            DateTime = DateTime.Now.AddSeconds(-5),
            SourceEntity = new Player { Id = 77 },
            TargetEntity = dummy,
            Skill = new Skill { Id = 14010000, Name = "Deadshot" },
            CharacterClass = new CharacterClass { Id = 14, Name = "Ranger" },
            Damage = 500,
        });
        sessions.CompleteActiveSessions();

        var skill = Assert.Single(Assert.Single(Assert.Single(history.Recent).Players).Skills);
        Assert.Equal(20, skill.Level);
    }

    // ---------------------------------------------------------------- the Compare window's rows

    private static EncounterSnapshot Fight(DateTime at, params EncounterSkill[] skills) => new()
    {
        StartedAt = at,
        DurationSeconds = 60,
        Players = [new EncounterPlayer { IsUser = true, Name = "Owner", Skills = skills.ToList() }],
    };

    private static EncounterSkill S(long id, string name, double dps, int level, params int[] spec) => new()
    {
        SkillId = id, Name = name, Dps = dps, TotalDamage = (long)(dps * 60), Level = level,
        Spec = Enumerable.Range(1, 5).Select(spec.Contains).ToArray(),
    };

    [Fact]
    public void Compare_OneRowPerSkill_StrongestFirst_WithAGapWhereASkillWasNotUsed()
    {
        var a = Fight(DateTime.Today.AddHours(10), S(14010000, "Deadshot", 5000, 20), S(14050000, "Arrow Storm", 3000, 16));
        var b = Fight(DateTime.Today.AddHours(11), S(14010000, "Deadshot", 5200, 20), S(14080000, "Snipe", 6000, 20));

        var rows = FightComparison.Rows([a, b]);

        Assert.Equal(["Snipe", "Deadshot", "Arrow Storm"], rows.Select(r => r.Name).ToArray());
        Assert.Null(rows[0].Cells[0]);                   // Snipe: not used in A
        Assert.Null(rows[2].Cells[1]);                   // Arrow Storm: not used in B
        Assert.Equal(5200, rows[1].BestDps);
    }

    [Fact]
    public void Compare_FlagsALevelDifference_ButNotAnUnknownLevel()
    {
        var a = Fight(DateTime.Today, S(14010000, "Deadshot", 5000, 20), S(14050000, "Arrow Storm", 3000, 0));
        var b = Fight(DateTime.Today, S(14010000, "Deadshot", 4800, 16), S(14050000, "Arrow Storm", 3100, 16));

        var rows = FightComparison.Rows([a, b]).ToDictionary(r => r.Name);

        Assert.True(rows["Deadshot"].LevelDiffers);
        Assert.False(rows["Arrow Storm"].LevelDiffers);  // A's level unknown (an older fight): no claim
    }

    [Fact]
    public void Compare_FlagsASpecialisationDifference()
    {
        var a = Fight(DateTime.Today, S(14010000, "Deadshot", 5000, 20, 2, 4, 5));
        var b = Fight(DateTime.Today, S(14010000, "Deadshot", 5100, 20, 1, 4, 5));

        var row = Assert.Single(FightComparison.Rows([a, b]));

        Assert.True(row.SpecDiffers);
        Assert.False(row.LevelDiffers);
        Assert.Equal("2 4 5", FightComparison.SpecText(row.Cells[0]!.Spec));
    }

    [Fact]
    public void Compare_UsesTheTopPlayer_WhenYouWereNotKnownYet()
    {
        var old = new EncounterSnapshot
        {
            Players = [new EncounterPlayer { IsUser = false, Name = "Top", Skills = [S(14010000, "Deadshot", 100, 0)] }],
        };
        Assert.Equal("Top", FightComparison.PlayerOf(old)!.Name);
    }
}
