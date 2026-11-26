using Hawkeye.Engine.PacketProcessing.Processors;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Entity;

namespace Hawkeye.Tests;

/// <summary>
/// Combat power on the meter (v0.2.35; owner, 2026-11-26: "shows CP of a person next to the name when the dungeon
/// loads"). The game sends other players' combat power only in the group list (0x9702); yours also comes in 0x3656.
/// The group list below is built field by field in the layout PartyProcessor reads (made-up names and ids); on 114
/// real lists that layout gave the owner's own entry exactly his 0x3656 value.
/// </summary>
public sealed class CombatPowerTests
{
    private static void Str(List<byte> b, string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        b.Add((byte)bytes.Length);
        b.AddRange(bytes);
    }

    /// <summary>A whole 0x9702 message: party header, then each member (mask 0x0C: both server ids, no extras).</summary>
    private static byte[] PartyList(params (string Name, uint Id, ulong Cp)[] members)
    {
        var b = new List<byte> { 0x02, 0x97 };
        b.AddRange(BitConverter.GetBytes(77u));            // party key
        Str(b, "");                                        // party name
        b.Add((byte)members.Length);                       // size
        b.AddRange(BitConverter.GetBytes(0u));             // dungeon id
        b.Add(0); b.Add(0);
        b.AddRange(BitConverter.GetBytes(0UL));            // leader
        b.Add(0x00);                                       // bit byte (the members' login bits are read from it)
        b.Add(0); b.Add(0);
        b.Add((byte)members.Length);                       // member count (varint)
        byte slot = 1;
        foreach (var (name, id, cp) in members)
        {
            b.Add(0x0C);                                   // presence mask
            b.Add(slot++);
            b.AddRange(BitConverter.GetBytes(((ulong)1304 << 48) | id));
            Str(b, name);
            b.AddRange(BitConverter.GetBytes(0u));
            b.AddRange(BitConverter.GetBytes(45u));        // level
            b.AddRange(BitConverter.GetBytes(3270u));      // gear score
            b.AddRange(BitConverter.GetBytes((ushort)1304)); // born server
            b.AddRange(BitConverter.GetBytes((ushort)1304)); // current server
            b.Add(0);                                      // role
            b.AddRange(BitConverter.GetBytes(cp));         // combat power
            b.Add(0);                                      // tickets
            b.Add(0); b.Add(0);                            // mentoring, latency
        }
        int length = b.Count + 2;
        return [(byte)(length & 0x7F | 0x80), (byte)(length >> 7), .. b];
    }

    private static byte[] OwnCp(ulong current, ulong highest)
    {
        var b = new List<byte> { 19, 0x56, 0x36 };
        b.AddRange(BitConverter.GetBytes(current));
        b.AddRange(BitConverter.GetBytes(highest));
        return [.. b];
    }

    [Fact]
    public void GroupMembers_GetTheirCombatPower_WhenTheGroupListComesAfterThem()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(501, "Bravo", 0);    // announced as they come into view
        entities.SetSessionPlayerName(502, "Zoë", 0);

        new PartyProcessor(entities).Process(new Packet { Data = PartyList(("Bravo", 9001, 117_375), ("Zoë", 9002, 165_767)) });

        Assert.Equal(117_375, entities.GetPlayerEntity(501)!.CombatPower);
        Assert.Equal(165_767, entities.GetPlayerEntity(502)!.CombatPower);
    }

    [Fact]
    public void GroupMembers_GetTheirCombatPower_WhenTheyAppearAfterTheGroupList()
    {
        // The dungeon loads: the group list first, then the members show up with new ids.
        var entities = new EntityTracker();
        new PartyProcessor(entities).Process(new Packet { Data = PartyList(("Bravo", 9001, 117_375)) });

        entities.SetSessionPlayerName(733, "Bravo", 0);

        Assert.Equal(117_375, entities.GetPlayerEntity(733)!.CombatPower);
    }

    [Fact]
    public void ANewGroupList_UpdatesTheNumber()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(501, "Bravo", 0);
        var party = new PartyProcessor(entities);
        party.Process(new Packet { Data = PartyList(("Bravo", 9001, 117_375)) });
        party.Process(new Packet { Data = PartyList(("Bravo", 9001, 119_005)) });

        Assert.Equal(119_005, entities.GetPlayerEntity(501)!.CombatPower);
    }

    [Fact]
    public void PlayersOutsideYourGroup_HaveNone()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(600, "Stranger", 0);
        new PartyProcessor(entities).Process(new Packet { Data = PartyList(("Bravo", 9001, 117_375)) });

        Assert.Equal(0, entities.GetPlayerEntity(600)!.CombatPower);
    }

    [Fact]
    public void YourCombatPower_FollowsTheGameMessage()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(301, "Owner", 112_000, isUser: true);
        entities.SetSessionPlayerName(302, "Other", 0);
        var own = new OwnCombatPowerProcessor(entities);

        own.Process(new Packet { Data = OwnCp(234_776, 239_918) });   // e.g. swapped to another gear set

        Assert.Equal(234_776, entities.GetPlayerEntity(301)!.CombatPower);
        Assert.Equal(0, entities.GetPlayerEntity(302)!.CombatPower);

        own.Process(new Packet { Data = OwnCp(250_000, 239_918) });   // current above highest: not this message
        Assert.Equal(234_776, entities.GetPlayerEntity(301)!.CombatPower);
    }
}
