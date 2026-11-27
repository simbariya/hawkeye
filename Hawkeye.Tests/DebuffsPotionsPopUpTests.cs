using Hawkeye.Engine.PacketProcessing.Processors;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Theostone;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// v0.2.38 (owner, 2026-11-26): debuffs on you, debuff Theostones that never showed, potion cooldowns, tracker icons
/// that pop up near the end. Messages are real ones from the owner's recordings (entity numbers only, no names).
/// </summary>
public sealed class DebuffsPotionsPopUpTests : IDisposable
{
    private readonly string settingsPath = Path.Combine(Path.GetTempPath(), $"hawkeye-debuff-{Guid.NewGuid():N}.json");
    private readonly SettingsService settings;
    private DateTime now = new(2026, 11, 26, 18, 0, 0);

    public DebuffsPotionsPopUpTests() => settings = new SettingsService(settingsPath);
    public void Dispose() { try { File.Delete(settingsPath); } catch (IOException) { } }

    private static Packet Msg(string hex) => new() { Data = Convert.FromHexString(hex) };

    private (EntityTracker Entities, TheostoneAlerts Alerts, CombatSessionManager Sessions, TimedEventTracker Buffs, TimedEventTracker Cooldowns) World(int me)
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(me, "Alpha", 0, isUser: true);
        var buffs = new TimedEventTracker();
        var cooldowns = new TimedEventTracker();
        var sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, settings, buffs, cooldowns);
        return (entities, new TheostoneAlerts(entities, settings) { Clock = () => now }, sessions, buffs, cooldowns);
    }

    private static BuffPacketProcessor Buffs(CombatSessionManager sessions, TheostoneAlerts alerts) =>
        new(NullLogger<BuffPacketProcessor>.Instance, sessions, alerts);

    // ---- the caster in buff messages ----

    // PvP recording 2026-11-21, the owner was 12526. Khrudgelmir's Silence on him, 4 s, from entity 11274 (8A 58).
    private const string Silence4s = "342A38EE610113E70AD3EEC901A00F0000000000005F2496FFA10100008A580115CB2D0000238BC0C5927002C678D32447";

    [Fact]
    public void YourDebuffStoneOnAnEnemy_IsYours()
    {
        // Seen from the caster's side: until v0.2.38 the caster was read one byte late (88 instead of 11274), so your
        // own debuff stones never showed as yours.
        var w = World(11274);
        Buffs(w.Sessions, w.Alerts).Process(Msg(Silence4s));
        var d = Assert.Single(w.Alerts.Showing);
        Assert.Equal((true, "Silence", 12526, TimeSpan.FromSeconds(4)), (d.Yours, d.Word, d.Target, d.Duration));
    }

    [Fact]
    public void ADebuffStoneOnYou_NamesWhoDidIt()
    {
        var w = World(12526);
        w.Entities.SetSessionPlayerName(11274, "Bravo", 0);
        Buffs(w.Sessions, w.Alerts).Process(Msg(Silence4s));
        var d = Assert.Single(w.Alerts.Showing);
        Assert.Equal((false, "Bravo", false), (d.Yours, d.From, d.Status));
    }

    // ---- debuff Theostones: their proc is a hit with no damage (mask 0) ----

    // Battlefield recording 2026-11-23: entity 10445 procs 3001109 (Epic Silence) on 14667. Mask 0, no damage.
    private const string SilenceProc = "230438CB720001CD5115CB2D00AF063F54E31101000000CD7E01D3EEC9010100";

    [Fact]
    public void ADebuffStoneProc_ShowsEvenWhenTheDebuffDoesNotLand()
    {
        var w = World(10445);
        new DamagePacketProcessor(w.Entities, w.Sessions, NullLogger<DamagePacketProcessor>.Instance, w.Alerts).Process(Msg(SilenceProc));
        var p = Assert.Single(w.Alerts.Showing);
        Assert.Equal((true, "Silence", 0L, false), (p.Yours, p.Word, p.Damage, p.IsDebuff));
        now = now.Add(w.Alerts.ShowFor);
        Assert.Empty(w.Alerts.Showing);    // a plain proc alert: gone after the usual few seconds
    }

    // ---- other debuffs on you ----

    // PvP recording: Defense Break on the owner (12526), 10 s, instance 1004 (EC 07), from entity 6668 (8C 34).
    private const string DefenseBreak = "342A38EE610113EC0787A93608102700000000000026368FFFA10100008C34052744D200007D1862C6DF06854574C72A47";
    // Fire Mark on him, 5 s, instance 917, from 2880; later another Fire Mark refreshed (0x382B), instance 1085, from 9213.
    private const string FireMark = "342A38EE610113950727285D09881300000000000035998CFFA1010000C016015FD1E5000094AC29C512A434C4CFD52747";
    private const string FireMarkRefreshed = "332B38EE6113BD0827285D098813000000000000E02491FFA1010000FD470137B7EF00022F4108C6F04F24C5A1001F47";
    // His own Drill Dart: 3 s on himself, and 6 s on entity 1401 (both cast by him).
    private const string DrillDartSelf = "342A38EE6101138D0771DC5F08B80B000000000000C6898CFFA1010000EE6101D862D60000A89300C5D84183C493B32C47";
    private const string DrillDartOnEnemy = "342A38F90A01138C4721DC5F0870170000000000001D9D8CFFA1010000EE61112E64D60000A89300C5D84183C493B32C47";

    [Fact]
    public void AnEnemysDebuffOnYou_ShowsWithItsNameAndWho()
    {
        var w = World(12526);
        w.Entities.SetSessionPlayerName(6668, "Bravo", 0);
        Buffs(w.Sessions, w.Alerts).Process(Msg(DefenseBreak));
        var d = Assert.Single(w.Alerts.Showing);
        Assert.Equal((true, "Defense Break", "Bravo", 1004, TimeSpan.FromSeconds(10)), (d.Status, d.Word, d.From, d.Instance, d.Duration));
        Assert.False(d.Yours);
    }

    // PvP recording, general effects on the owner (12526) with no name in the game data. Each caused by a skill whose
    // effect is known: Shadowstrike Stun 3 s (from 6668), Poach Root 2 s (4547), Doom Shield Knockdown 3 s (14047),
    // Smoke Bomb Blind 5 s (5626).
    private const string Stun3s = "342A38EE610113EA072DE2F505B80B0000000000006A1A8FFFA10100008C3401966FC70000A82062C6CA528545C7C22A47";
    private const string Root2s = "342A38EE6101138209C9E1F505D0070000000000005DE991FFA1010000C323012418B900000C1E2EC6DD8FF944D3212447";
    private const string Knockdown3s = "342A38EE610113A90941E2F505B80B000000000000995C92FFA1010000DF6D01982CB800007A54C6C52305E3C500D21E47";
    private const string Blind5s = "342A38EE6101139908BDE3F505881300000000000099EB8FFFA1010000FA2B01F82DCA00007E21BBC5ADDF28C639AC2347";

    [Theory]
    [InlineData(Stun3s, "Stun", 3)]
    [InlineData(Root2s, "Root", 2)]
    [InlineData(Knockdown3s, "Knockdown", 3)]
    [InlineData(Blind5s, "Blind", 5)]
    public void AGeneralEffectOnYou_SaysWhatItIs(string message, string word, int seconds)
    {
        // Owner 2026-11-27: "can debuff on you be classified? instead of just debuff".
        var w = World(12526);
        Buffs(w.Sessions, w.Alerts).Process(Msg(message));
        var d = Assert.Single(w.Alerts.Showing);
        Assert.Equal((true, word, TimeSpan.FromSeconds(seconds)), (d.Status, d.Word, d.Duration));
    }

    [Fact]
    public void ADebuffOnYou_EndsWhenCleansed()
    {
        var w = World(12526);
        Buffs(w.Sessions, w.Alerts).Process(Msg(DefenseBreak));
        new BuffRemovedProcessor(w.Alerts).Process(Msg("0B2C38EE610107EC07"));   // [entity 12526][1][reason 7][instance 1004]
        Assert.Empty(w.Alerts.Showing);
    }

    [Fact]
    public void TheSameDebuffAgain_IsOneTile()
    {
        var w = World(12526);
        var reader = Buffs(w.Sessions, w.Alerts);
        reader.Process(Msg(FireMark));
        now = now.AddSeconds(2);
        reader.Process(Msg(FireMarkRefreshed));
        var d = Assert.Single(w.Alerts.Showing);
        Assert.Equal(("Fire Mark", 1085), (d.Word, d.Instance));
        Assert.Equal(TimeSpan.FromSeconds(5), w.Alerts.TimeLeft(d));
    }

    [Fact]
    public void YourGroupsEffectsAndYourOwn_AreNotDebuffs()
    {
        var w = World(12526);
        w.Entities.SetSessionPlayerName(6668, "Bravo", 0);
        w.Entities.AddToGroup(["Alpha", "Bravo"]);
        var reader = Buffs(w.Sessions, w.Alerts);
        reader.Process(Msg(DefenseBreak));      // from someone in your group
        reader.Process(Msg(DrillDartSelf));     // your own
        Assert.Empty(w.Alerts.Showing);
    }

    [Fact]
    public void DebuffsOnYou_CanBeSwitchedOff()
    {
        settings.Update(s => s.DebuffAlerts = false);
        var w = World(12526);
        Buffs(w.Sessions, w.Alerts).Process(Msg(DefenseBreak));
        Assert.Empty(w.Alerts.Showing);
    }

    [Fact]
    public void BuffMode_StillFollowsOnlyTheBuffOnYou()
    {
        // The caster is read right now, so "cast by you" is real; the tracker must not start following your Drill
        // Dart mark on an enemy (6 s) in place of the buff on you (3 s).
        settings.Update(s => s.TrackedSkills.Add(new TrackedSkill { SkillId = 14050000, Mode = TrackMode.Buff }));
        var w = World(12526);
        var reader = Buffs(w.Sessions, w.Alerts);
        reader.Process(Msg(DrillDartOnEnemy));
        Assert.Null(w.Buffs.Get(14050000));
        reader.Process(Msg(DrillDartSelf));
        Assert.Equal(TimeSpan.FromSeconds(3), w.Buffs.Get(14050000)!.Duration);
    }

    // ---- potions ----

    // The owner drinking a Superior Healing Potion (2020102), PvP recording 19:20:33. Its cooldown field is 0.
    private const string SuperiorHealingPotion = "190238EE610006D31E004600EE618A031143904E0200";

    [Fact]
    public void APotion_StartsItsCooldownForTheWholeFamily()
    {
        var w = World(12526);
        new SkillCastProcessor(w.Sessions).Process(Msg(SuperiorHealingPotion));
        Assert.Equal(TimeSpan.FromSeconds(10), w.Cooldowns.Get(2020102)!.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), w.Cooldowns.Get(2020101)!.Duration);   // the plain one shares it
        Assert.Null(w.Cooldowns.Get(2011101));                                        // Life Potion: its own
    }

    [Fact]
    public void SomeoneElsesPotion_DoesNothing()
    {
        var w = World(777);
        new SkillCastProcessor(w.Sessions).Process(Msg(SuperiorHealingPotion));
        Assert.Null(w.Cooldowns.Get(2020102));
    }

    [Fact]
    public void PotionsCanBePicked_AndHaveTheirNames()
    {
        var data = Hawkeye.Core.GameData.Services.GameDataProvider.Instance;
        Assert.Equal(("Superior Healing Potion", 2020102), (data.GetSkillOrDefault(2020102).Name, data.GetSkillOrDefault(2020102).Id));
        Assert.True(PotionCooldowns.IsKnown(2011101));
        Assert.Equal(TimeSpan.FromSeconds(30), PotionCooldowns.Cooldown(2011102));
    }

    // ---- pop up ----

    [Fact]
    public void PopUp_ShowsOnlyNearTheEnd_AndAMomentAfter()
    {
        var t = new DateTime(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);
        Assert.True(PopUp.Visible(0, null, DateTime.MinValue, t));                          // off: always
        Assert.False(PopUp.Visible(3, TimeSpan.FromSeconds(8), t, t));                     // 8 s left: hidden
        Assert.True(PopUp.Visible(3, TimeSpan.FromSeconds(2.9), t, t));                     // last 3 s: shown
        Assert.True(PopUp.Visible(3, null, t, t.AddSeconds(2)));                            // ready / ran out 2 s ago
        Assert.False(PopUp.Visible(3, null, t, t + PopUp.Linger + TimeSpan.FromSeconds(0.1)));
        Assert.False(PopUp.Visible(3, null, DateTime.MinValue, t));                         // never ran: hidden
    }
}
